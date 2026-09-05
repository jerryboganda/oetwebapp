using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Services.Speaking;

public sealed class SpeakingSimulationV11AssessmentService(
    LearnerDbContext db,
    IAiGatewayService aiGateway,
    SpeakingSimulationV11ReleaseGate releaseGate,
    SpeakingSimulationV11EvidenceCaptureService evidenceCapture,
    SpeakingSimulationV11AudioAssessmentService audioAssessment,
    SpeakingSimulationV11TurnTelemetryService telemetry,
    ILogger<SpeakingSimulationV11AssessmentService> logger)
{
    private const string PromptTemplateId = "speaking.simulation.v1.1.assessment";
    private const string CardKind = "card";
    private const string CombinedKind = "combined";

    private const string AssessmentPrompt = """
You are the calibrated assessor for an OET Speaking AI simulation.
Return ONLY one strict JSON object. Do not return markdown or prose.

The object must contain:
criteria: an array containing exactly one item for each of:
intelligibility_pronunciation (10), fluency_continuity (12),
grammar_vocabulary (8), appropriateness_plain_language (10),
relationship_building_empathy (14), patient_perspective (10),
information_gathering (10), information_giving_checking (12),
structure_task_management (9), closure_time_management (5).
Each item has criterionCode, score (0-100), rationale, strength, weakness,
action, confidenceLabel, confidenceScore, and evidence[].
Each evidence item has evidenceType, turnNumber, quote, finding, action,
confidenceLabel, confidenceScore. Also return overallSummary, strengths[],
weaknesses[], taskMap[], timeline[], languageAnalysis{}, timeManagement{},
topFive[], betterAlternatives[], tips[], practicePlan[], and confidence
{label,score,rangeLow,rangeHigh}.

Rules:
- The server applies the ten released weights and computes the 0-500 AI
  Estimated Practice Score. Never call it an official OET result.
- Evidence belongs only to its enclosing criterion: one primary criterion per
  finding and never deduct the same event from multiple criteria.
- quote must be an exact candidate-transcript substring. Never quote the
  patient, hidden persona, or an invented sentence.
- ASR confidence errors are not candidate communication errors.
- Use only the supplied source turns and server timing; never invent timestamps,
  audio findings, or medical advice.
""";

    public async Task<SpeakingSimulationV11AssessmentResponse> RunAssessmentAsync(
        string sessionId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            throw ApiException.Validation("SPEAKING_SESSION_ID_REQUIRED", "Speaking session id is required.");

        var session = await db.SpeakingSessions.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == sessionId, ct)
            ?? throw ApiException.NotFound("speaking_session_not_found", "That Speaking session does not exist.");
        if (session.State != SpeakingSessionState.Finished)
        {
            throw ApiException.Conflict(
                "speaking_session_not_finished",
                "The v1.1 assessment is available only after the server-authoritative role-play has finished.");
        }
        var card = await db.RolePlayCards.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == session.RolePlayCardId, ct)
            ?? throw ApiException.NotFound("role_play_card_not_found", "That role-play card does not exist.");

        if (!RulebookProfessionParser.TryParse(card.ProfessionId, out var profession))
        {
            return await TechnicalAsync(session, card, SpeakingSimulationV11AudioQualityStatus.Failed,
                "unsupported_profession",
                "This card has no explicit profession pack. No Medicine fallback is permitted.", ct);
        }

        var isMock = !string.IsNullOrWhiteSpace(session.MockSetId)
            || !string.IsNullOrWhiteSpace(session.MockSessionId);
        if (session.Mode == SpeakingSessionMode.LiveTutor && !isMock)
            return await TechnicalAsync(session, card, SpeakingSimulationV11AudioQualityStatus.Pending,
                "human_examiner_required", "This session is marked for human examiner assessment.", ct);

        var hasPersonaRuntime = await db.SpeakingSimulationV11PersonaRuntimeSnapshots
            .AsNoTracking()
            .AnyAsync(x => x.SpeakingSessionId == sessionId, ct);
        if (!hasPersonaRuntime)
        {
            return await TechnicalAsync(session, card, SpeakingSimulationV11AudioQualityStatus.Pending,
                "v11_persona_not_captured",
                "The approved v1.1 actor/persona snapshot was not captured at card reveal. No score was generated.",
                ct);
        }

        var existing = await db.SpeakingSimulationV11Assessments.AsNoTracking()
            .Where(x => x.SpeakingSessionId == sessionId
                && x.AssessmentKind == CardKind
                && x.Status == SpeakingSimulationV11AssessmentStatus.Complete)
            .OrderByDescending(x => x.GeneratedAt)
            .FirstOrDefaultAsync(ct);
        if (existing is not null)
            return Project(existing, ReadReport(existing.ReportJson));

        var gate = await releaseGate.EvaluateAsync(card.ProfessionId, ct);
        if (!gate.IsReleased)
            throw ApiException.Conflict(
                "speaking_v11_release_blocked",
                "The v1.1 Speaking simulation is not released for assessment.",
                gate.BlockingReasons.Select(x => new ApiFieldError(
                    "releaseGate", x, "Owner approval is required before v1.1 can score learner evidence.")));
        var rubricCriteria = gate.RubricCriteria is { Count: > 0 }
            ? gate.RubricCriteria
            : SpeakingSimulationV11Contracts.RubricCriteria.Criteria;

        if (await telemetry.HasTechnicalReviewBreachAsync(sessionId, ct))
        {
            return await TechnicalAsync(session, card, SpeakingSimulationV11AudioQualityStatus.NeedsReview,
                "turn_technical_review_required",
                "A live turn encountered an approved operational or audio-degradation gate. No score was generated until technical review is complete.",
                ct);
        }

        var capture = await evidenceCapture.CaptureAsync(sessionId, ct);
        var quality = await db.SpeakingSimulationV11AudioQualityChecks.AsNoTracking()
            .Where(x => x.SpeakingSessionId == sessionId)
            .OrderByDescending(x => x.CheckedAt)
            .FirstOrDefaultAsync(ct);
        var sourceTranscript = await db.SpeakingTranscripts.AsNoTracking()
            .Where(x => x.SpeakingSessionId == sessionId && x.IsLatest)
            .OrderByDescending(x => x.GeneratedAt)
            .FirstOrDefaultAsync(ct);
        var transcriptHash = SpeakingCanonicalAssessmentService.HashTranscript(sourceTranscript?.SegmentsJson);
        var identityHash = SpeakingCanonicalAssessmentService.HashIdentity(
            sessionId,
            session.RolePlayCardId,
            transcriptHash,
            gate.RubricVersion,
            PromptTemplateId);
        var existingByIdentity = await db.SpeakingSimulationV11Assessments.AsNoTracking()
            .Where(x => x.IdentityHash == identityHash)
            .OrderBy(x => x.GeneratedAt)
            .FirstOrDefaultAsync(ct);
        if (existingByIdentity is not null)
            return Project(existingByIdentity, ReadReport(existingByIdentity.ReportJson));
        var turnQuery = db.SpeakingSimulationV11TurnEvidenceRows.AsNoTracking()
            .Where(x => x.SpeakingSessionId == sessionId);
        if (sourceTranscript is not null)
        {
            turnQuery = turnQuery.Where(x => x.SourceTranscriptId == sourceTranscript.Id);
        }
        var turns = await turnQuery.OrderBy(x => x.TurnNumber).ToListAsync(ct);

        if (!capture.SourceTranscriptAvailable || turns.Count == 0)
            return await TechnicalAsync(session, card, quality?.Status ?? capture.AudioQualityStatus,
                "transcript_unavailable",
                "The authoritative role-play transcript is missing or contains no valid turns. No score was generated.",
                ct);
        if (quality is null || quality.Status is SpeakingSimulationV11AudioQualityStatus.NeedsReview
            or SpeakingSimulationV11AudioQualityStatus.Failed)
            return await TechnicalAsync(session, card, quality?.Status ?? capture.AudioQualityStatus,
                quality?.IssueCode ?? capture.AudioQualityIssueCode ?? "original_audio_unverified",
                "The original role-play audio could not be verified. No score was generated.", ct);

        var candidates = turns.Where(IsCandidate).ToList();
        if (candidates.Count == 0)
            return await TechnicalAsync(session, card, quality.Status, "candidate_turns_missing",
                "The authoritative transcript contains no candidate turns. No score was generated.", ct);

        var acoustic = await audioAssessment.AssessAsync(
            session.UserId, card.ProfessionId, candidates, gate.AudioAssessmentProvider, ct);
        if (!acoustic.IsAvailable)
        {
            return await TechnicalAsync(session, card, quality.Status,
                acoustic.IssueCode ?? "audio_assessment_unavailable",
                acoustic.Summary, ct);
        }

        var transcriptId = turns.Select(x => x.SourceTranscriptId)
            .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
        var recordingId = quality.SourceRecordingId ?? turns.Select(x => x.SourceRecordingId)
            .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
        var runtime = await db.SpeakingSimulationV11PersonaRuntimeSnapshots.AsNoTracking()
            .FirstOrDefaultAsync(x => x.SpeakingSessionId == sessionId, ct);
        var cardVersion = runtime?.CardVersion
            ?? (card.UpdatedAt == default ? "unversioned"
                : card.UpdatedAt.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
        var timing = await db.SpeakingSimulationV11CardTimingSnapshots.AsNoTracking()
            .Where(x => x.SpeakingSessionId == sessionId)
            .OrderByDescending(x => x.CapturedAt)
            .FirstOrDefaultAsync(ct);

        var prompt = aiGateway.BuildGroundedPrompt(new AiGroundingContext
        {
            Kind = RuleKind.Speaking,
            Profession = profession,
            Task = AiTaskMode.Score,
            CardType = "role_play",
        });
        var input = BuildInput(card, turns, timing, rubricCriteria);
        var callStartedAt = DateTimeOffset.UtcNow;
        var watch = Stopwatch.StartNew();
        AiGatewayResult result;
        try
        {
            result = await aiGateway.CompleteAsync(new AiGatewayRequest
            {
                Prompt = prompt,
                UserInput = input,
                Temperature = 0.1,
                MaxTokens = 6000,
                FeatureCode = AiFeatureCodes.SpeakingGrade,
                UserId = session.UserId,
                PromptTemplateId = PromptTemplateId,
                AssessmentContext = AiAssessmentContext.Practice,
            }, ct);
        }
        catch (PromptNotGroundedException) { throw; }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "v1.1 assessment gateway failed for {SessionId}.", sessionId);
            return await TechnicalAsync(session, card, quality.Status, "ai_unavailable",
                "The calibrated assessment service was unavailable. Retry after the technical issue is resolved.", ct);
        }
        finally { watch.Stop(); }

        // AiGatewayService records the authoritative provider/model/token/cost
        // row. Correlate by the stable feature and call window so the v1.1
        // metric does not invent a rate card or silently report zero cost when
        // the usage ledger is available.
        AiUsageRecord? usageRow = null;
        if (!string.IsNullOrWhiteSpace(result.UsageRecordId))
        {
            usageRow = await db.AiUsageRecords.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == result.UsageRecordId
                    && x.UserId == session.UserId
                    && x.FeatureCode == AiFeatureCodes.SpeakingGrade
                    && x.Outcome == AiCallOutcome.Success, ct);
        }

        // Keep a narrow time-window fallback for older gateway providers that
        // returned usage metadata before the stable ledger id was available.
        // The stable id is authoritative whenever it is present, preventing a
        // concurrent assessment from being charged to this report.
        usageRow ??= await db.AiUsageRecords.AsNoTracking()
            .Where(x => x.UserId == session.UserId
                && x.FeatureCode == AiFeatureCodes.SpeakingGrade
                && x.Outcome == AiCallOutcome.Success
                && x.CreatedAt >= callStartedAt)
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(ct);

        var budget = await releaseGate.GetOperationalBudgetAsync(
            gate.SpecVersion, gate.RubricVersion, ct);
        if (budget is null)
        {
            return await TechnicalAsync(session, card, quality.Status, "operational_budget_missing",
                "The approved v1.1 latency, concurrency, cost, and retention budget is unavailable. No score was generated.", ct);
        }
        if (usageRow is null || result.Usage is null)
        {
            return await TechnicalAsync(session, card, quality.Status, "usage_ledger_missing",
                "The assessment usage and cost ledger could not be correlated. No score was generated.", ct);
        }
        var priorTurnCost = await db.SpeakingSimulationV11TurnTelemetryRows.AsNoTracking()
            .Where(x => x.SpeakingSessionId == sessionId)
            .Select(x => (decimal?)x.EstimatedCostUsd)
            .SumAsync(ct) ?? 0m;
        var totalAttemptCost = priorTurnCost + usageRow.CostEstimateUsd;
        if (totalAttemptCost > budget.CostCeilingUsd)
        {
            return await TechnicalAsync(session, card, quality.Status, "cost_ceiling_exceeded",
                "The approved v1.1 cost ceiling was exceeded. No score was generated.", ct);
        }
        if (watch.ElapsedMilliseconds > budget.LatencySlaMs)
        {
            return await TechnicalAsync(session, card, quality.Status, "latency_sla_exceeded",
                "The approved v1.1 latency SLA was exceeded. No score was generated.", ct);
        }

        var parsed = ParseAssessment(result.Completion, rubricCriteria);
        if (parsed is null)
            return await TechnicalAsync(session, card, quality.Status, "ai_payload_invalid",
                "The calibrated assessment response did not satisfy the v1.1 JSON contract. No score was generated.", ct);

        var assessmentId = "spv11_assess_" + Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow;
        var criterionResults = new List<SpeakingSimulationV11CriterionResult>();
        var evidenceRows = new List<SpeakingSimulationV11Evidence>();
        // Language evidence owns one primary criterion per detected
        // communication behaviour. Acoustic evidence is a separate audio-only
        // dimension and must not reserve the source turn for pronunciation;
        // doing so would make every other criterion appear unverified simply
        // because the same turn also has an acoustic measurement. Different
        // behaviours in one turn may each have primary ownership, while the
        // same evidence fingerprint can only be primary once.
        var evidencePrimaryCriteria = new Dictionary<string, string>(StringComparer.Ordinal);
        var acousticCriterionCode = "intelligibility_pronunciation";

        foreach (var rubric in rubricCriteria)
        {
            var parsedCriterion = parsed.Criteria[rubric.CriterionCode];
            var evidence = new List<SpeakingSimulationV11EvidenceResult>();
            var primaryEvidenceCount = 0;

            if (string.Equals(rubric.CriterionCode, acousticCriterionCode, StringComparison.Ordinal)
                && acoustic.Score is not null)
            {
                foreach (var source in candidates.Where(x => acoustic.SourceRecordingIds.Contains(
                             x.SourceRecordingId ?? string.Empty, StringComparer.Ordinal)))
                {
                    evidence.Add(new SpeakingSimulationV11EvidenceResult(
                        "audio_acoustic", "supported", rubric.CriterionCode, source.TurnNumber,
                        source.Text, ToInt(source.StartMs), ToInt(source.EndMs),
                        acoustic.Summary, "Continue practising intelligible, listener-friendly pronunciation.",
                        "high", 0.9m, transcriptId, source.SourceRecordingId, true));
                    evidenceRows.Add(new SpeakingSimulationV11Evidence
                    {
                        Id = "spv11_evidence_" + Guid.NewGuid().ToString("N"),
                        AssessmentId = assessmentId,
                        PrimaryCriterionCode = rubric.CriterionCode,
                        CriterionCode = rubric.CriterionCode,
                        EvidenceType = "audio_acoustic",
                        TurnNumber = source.TurnNumber,
                        SourceReference = "transcript:" + transcriptId + ":turn:" + source.TurnNumber,
                        QuoteText = source.Text,
                        StartMs = ToInt(source.StartMs),
                        EndMs = ToInt(source.EndMs),
                        EvidenceStatus = "supported",
                        FindingText = acoustic.Summary,
                        ActionSuggestion = "Continue practising intelligible, listener-friendly pronunciation.",
                        ConfidenceLabel = "high",
                        ConfidenceScore = 0.9m,
                        IsPrimary = true,
                        SourceTranscriptId = transcriptId,
                        SourceRecordingId = source.SourceRecordingId,
                        CardVersion = cardVersion,
                        GeneratedAt = now,
                        CreatedAt = now,
                    });
                    primaryEvidenceCount++;
                }
            }

            foreach (var item in parsedCriterion.Evidence)
            {
                var source = ResolveSource(candidates, item);
                var supported = source is not null;
                var verifiedQuote = supported
                    ? ExtractVerifiedQuote(source!.Text, item.Quote)
                    : string.Empty;
                var primaryCriterion = rubric.CriterionCode;
                var isPrimary = false;
                if (supported)
                {
                    var evidenceKey = EvidenceKey(source!, item, verifiedQuote);
                    if (evidencePrimaryCriteria.TryGetValue(evidenceKey, out var existingCriterion))
                    {
                        primaryCriterion = existingCriterion;
                    }
                    else
                    {
                        evidencePrimaryCriteria[evidenceKey] = rubric.CriterionCode;
                        isPrimary = true;
                        primaryEvidenceCount++;
                    }
                }
                var evidenceStatus = !supported
                    ? "unsupported"
                    : isPrimary ? "supported" : "teaching_only";
                evidence.Add(new SpeakingSimulationV11EvidenceResult(
                    item.EvidenceType, evidenceStatus, primaryCriterion,
                    supported ? source!.TurnNumber : null,
                    verifiedQuote,
                    supported ? ToInt(source!.StartMs) : null,
                    supported ? ToInt(source!.EndMs) : null,
                    item.Finding, item.Action, ConfidenceLabel(item.ConfidenceLabel),
                    ClampConfidence(item.ConfidenceScore),
                    supported ? transcriptId : null,
                    supported ? source!.SourceRecordingId : null,
                    isPrimary));
                evidenceRows.Add(new SpeakingSimulationV11Evidence
                {
                    Id = "spv11_evidence_" + Guid.NewGuid().ToString("N"),
                    AssessmentId = assessmentId,
                    PrimaryCriterionCode = primaryCriterion,
                    CriterionCode = rubric.CriterionCode,
                    EvidenceType = item.EvidenceType,
                    TurnNumber = supported ? source!.TurnNumber : null,
                    SourceReference = supported ? "transcript:" + transcriptId + ":turn:" + source!.TurnNumber : null,
                    QuoteText = verifiedQuote,
                    StartMs = supported ? ToInt(source!.StartMs) : null,
                    EndMs = supported ? ToInt(source!.EndMs) : null,
                    EvidenceStatus = evidenceStatus,
                    FindingText = item.Finding,
                    ActionSuggestion = item.Action,
                    ConfidenceLabel = ConfidenceLabel(item.ConfidenceLabel),
                    ConfidenceScore = ClampConfidence(item.ConfidenceScore),
                    IsPrimary = isPrimary,
                    SourceTranscriptId = transcriptId,
                    SourceRecordingId = supported ? source!.SourceRecordingId : null,
                    CardVersion = cardVersion,
                    GeneratedAt = now,
                    CreatedAt = now,
                });
            }
            if (primaryEvidenceCount == 0)
            {
                return await TechnicalAsync(session, card, quality.Status,
                    "criterion_evidence_unverified",
                    $"Criterion '{rubric.CriterionCode}' has no source-backed primary evidence. No score was generated.", ct);
            }

            var rawScore = string.Equals(rubric.CriterionCode, acousticCriterionCode, StringComparison.Ordinal)
                ? (decimal)(acoustic.Score ?? 0)
                : parsedCriterion.Score;
            var rationale = string.Equals(rubric.CriterionCode, acousticCriterionCode, StringComparison.Ordinal)
                ? $"{parsedCriterion.Rationale} {acoustic.Summary}"
                : parsedCriterion.Rationale;
            var weighted = Math.Round(rawScore * rubric.Weight / 100m, 2,
                MidpointRounding.AwayFromZero);
            criterionResults.Add(new SpeakingSimulationV11CriterionResult(
                rubric.CriterionCode, rubric.Label, rubric.Weight, rawScore, weighted,
                ScoreBand(rawScore), rationale, evidence,
                parsedCriterion.Strength, parsedCriterion.Weakness,
                parsedCriterion.Action ?? evidence.Select(x => x.Action).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)),
                ConfidenceLabel(parsedCriterion.ConfidenceLabel),
                ClampConfidence(parsedCriterion.ConfidenceScore)));
        }

        var overall = Math.Round(criterionResults.Sum(x => x.WeightedScore), 2,
            MidpointRounding.AwayFromZero);
        var estimated = Math.Clamp((int)Math.Round(overall * 5m, MidpointRounding.AwayFromZero), 0, 500);
        var confidence = ConfidenceLabel(parsed.ConfidenceLabel);
        var confidenceScore = ClampConfidence(parsed.ConfidenceScore);
        var low = parsed.RangeLow ?? Math.Max(0, estimated - RangeWidth(confidence));
        var high = parsed.RangeHigh ?? Math.Min(500, estimated + RangeWidth(confidence));
        if (low > high) (low, high) = (high, low);
        var slot = string.IsNullOrWhiteSpace(session.ExamSlot) ? "standalone" : session.ExamSlot;
        var report = new SpeakingSimulationV11AssessmentReport(
            assessmentId, CardKind, slot, gate.SpecVersion, gate.RubricVersion,
            gate.CalibrationVersion,
            SpeakingSimulationV11Contracts.GraphDisclaimer, estimated, low, high, confidence,
            confidenceScore, parsed.Summary, criterionResults,
            Array.Empty<SpeakingSimulationV11CardBreakdown>(), Limit(parsed.Strengths, 12),
            Limit(parsed.Weaknesses, 12), SanitizeTaskMap(parsed.TaskMap, candidates), BuildTimeline(turns), parsed.LanguageAnalysis,
            parsed.TimeManagement, Limit(parsed.TopFive, 5), SanitizeAlternatives(parsed.Alternatives, candidates),
            Limit(parsed.Tips, 5), parsed.PracticePlan.Take(12).ToArray(), transcriptId,
            recordingId, cardVersion, now);
        var row = new SpeakingSimulationV11Assessment
        {
            Id = assessmentId, ExamSessionId = session.ExamSessionId, SpeakingSessionId = session.Id,
            RolePlayCardId = card.Id, ProfessionId = card.ProfessionId,
            SpecVersion = gate.SpecVersion, RubricVersion = gate.RubricVersion,
            CalibrationVersion = gate.CalibrationVersion,
            AssessmentKind = CardKind, CardSlot = slot,
            Status = SpeakingSimulationV11AssessmentStatus.Complete,
            AudioQualityStatus = quality.Status, ConfidenceScore = confidenceScore,
            ConfidenceLabel = confidence, ConfidenceRange = low + "-" + high,
            EstimatedPracticeScore = estimated, ScoreRangeLow = low, ScoreRangeHigh = high,
            Provider = string.IsNullOrWhiteSpace(result.ResolvedProvider) ? "ai_gateway" : result.ResolvedProvider,
            ModelName = string.IsNullOrWhiteSpace(result.ResolvedModel) ? "gateway-default" : result.ResolvedModel,
            PromptTemplateId = PromptTemplateId, SourceTranscriptId = transcriptId,
            SourceRecordingId = recordingId, CardVersion = cardVersion,
            GraphDisclaimer = SpeakingSimulationV11Contracts.GraphDisclaimer,
            ReportJson = JsonSerializer.Serialize(report),
            IdentityHash = identityHash,
            TranscriptHash = transcriptHash,
            GeneratedAt = now, CreatedAt = now, UpdatedAt = now,
        };
        db.SpeakingSimulationV11Assessments.Add(row);
        db.SpeakingSimulationV11EvidenceRows.AddRange(evidenceRows);
        foreach (var criterion in criterionResults)
            db.SpeakingSimulationV11CriterionScores.Add(new SpeakingSimulationV11CriterionScore
            {
                Id = "spv11_score_" + Guid.NewGuid().ToString("N"),
                AssessmentId = assessmentId, CriterionCode = criterion.CriterionCode,
                Weight = criterion.Weight, RawScore = criterion.RawScore,
                WeightedScore = criterion.WeightedScore, ScoreBand = criterion.ScoreBand,
                Rationale = criterion.Rationale, CreatedAt = now,
            });
        if (runtime is not null)
            db.SpeakingSimulationV11PersonaSnapshots.Add(new SpeakingSimulationV11PersonaSnapshot
            {
                Id = "spv11_persona_" + Guid.NewGuid().ToString("N"), AssessmentId = assessmentId,
                RolePlayCardId = card.Id, PersonaRole = runtime.PersonaRole,
                ScenarioTitle = runtime.ScenarioTitle, Setting = runtime.Setting,
                CandidateRole = runtime.CandidateRole, InterlocutorRole = runtime.InterlocutorRole,
                PatientEmotion = runtime.PatientEmotion, CommunicationGoal = runtime.CommunicationGoal,
                ClinicalTopic = runtime.ClinicalTopic, PersonaJson = runtime.PersonaJson, CreatedAt = now,
            });
        db.SpeakingSimulationV11TurnMetrics.Add(new SpeakingSimulationV11TurnMetric
        {
            Id = "spv11_metric_" + Guid.NewGuid().ToString("N"), AssessmentId = assessmentId,
            TurnNumber = 0, ModelName = usageRow?.Model ?? row.ModelName ?? "gateway-default",
            PromptLatencyMs = 0, CompletionLatencyMs = (int)watch.ElapsedMilliseconds,
            TotalLatencyMs = (int)watch.ElapsedMilliseconds,
            InputTokens = result.Usage?.PromptTokens ?? 0,
            OutputTokens = result.Usage?.CompletionTokens ?? 0,
            EstimatedCostUsd = usageRow?.CostEstimateUsd ?? 0m,
            GeneratedAt = now,
        });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            var raced = await db.SpeakingSimulationV11Assessments.AsNoTracking()
                .FirstOrDefaultAsync(x => x.IdentityHash == identityHash, ct);
            if (raced is not null)
                return Project(raced, ReadReport(raced.ReportJson));
            throw;
        }
        await evidenceCapture.AttachAssessmentAsync(sessionId, assessmentId, ct);
        return Project(row, report);
    }

    public async Task<SpeakingSimulationV11AssessmentResponse?> GetLatestAsync(
        string sessionId, CancellationToken ct)
    {
        var row = await db.SpeakingSimulationV11Assessments.AsNoTracking()
            .Where(x => x.SpeakingSessionId == sessionId && x.AssessmentKind == CardKind)
            .OrderByDescending(x => x.GeneratedAt).FirstOrDefaultAsync(ct);
        return row is null ? null : Project(row, ReadReport(row.ReportJson));
    }

    public async Task<SpeakingSimulationV11AssessmentResponse> RunCombinedAssessmentAsync(
        string examSessionId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(examSessionId))
            throw ApiException.Validation("SPEAKING_EXAM_ID_REQUIRED", "Speaking exam id is required.");
        var exam = await db.SpeakingExamSessions.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == examSessionId, ct)
            ?? throw ApiException.NotFound("speaking_exam_not_found", "That Speaking exam does not exist.");
        if (exam.State != SpeakingExamState.Completed)
            return CombinedTechnical("speaking_exam_not_completed");
        if (exam.Mode == SpeakingExamMode.LiveTutor)
            return CombinedTechnical("human_examiner_required");

        var existingCombined = await db.SpeakingSimulationV11Assessments.AsNoTracking()
            .Where(x => x.ExamSessionId == examSessionId
                && x.AssessmentKind == CombinedKind
                && x.Status == SpeakingSimulationV11AssessmentStatus.Complete)
            .OrderByDescending(x => x.GeneratedAt)
            .FirstOrDefaultAsync(ct);
        if (existingCombined is not null)
            return Project(existingCombined, ReadReport(existingCombined.ReportJson));

        var sessionIds = new[] { exam.SessionAId, exam.SessionBId }
            .Where(x => !string.IsNullOrWhiteSpace(x)).Cast<string>().ToArray();
        if (sessionIds.Length != 2) return CombinedTechnical("two_cards_required");
        var cardRows = await db.SpeakingSimulationV11Assessments.AsNoTracking()
            .Where(x => sessionIds.Contains(x.SpeakingSessionId!) && x.AssessmentKind == CardKind)
            .ToListAsync(ct);
        var cards = cardRows
            .Where(x => !string.IsNullOrWhiteSpace(x.SpeakingSessionId))
            .GroupBy(x => x.SpeakingSessionId!, StringComparer.Ordinal)
            .Select(group => group.OrderByDescending(x => x.GeneratedAt).First())
            .ToList();
        if (cards.Count != 2 || cards.Any(x => x.Status != SpeakingSimulationV11AssessmentStatus.Complete))
            return CombinedTechnical("card_assessment_invalid");

        if (!RulebookProfessionParser.TryParse(exam.ProfessionId, out _)
            || cards.Any(card => !string.Equals(card.ProfessionId, exam.ProfessionId,
                StringComparison.OrdinalIgnoreCase)
                || !RulebookProfessionParser.TryParse(card.ProfessionId, out _)))
        {
            return CombinedTechnical("profession_mismatch");
        }

        var gate = await releaseGate.EvaluateAsync(exam.ProfessionId, ct);
        if (!gate.IsReleased)
        {
            return CombinedTechnical("release_blocked");
        }
        var rubricCriteria = gate.RubricCriteria is { Count: > 0 }
            ? gate.RubricCriteria
            : SpeakingSimulationV11Contracts.RubricCriteria.Criteria;

        var scoreRows = await db.SpeakingSimulationV11CriterionScores.AsNoTracking()
            .Where(x => cards.Select(c => c.Id).Contains(x.AssessmentId)).ToListAsync(ct);
        var cardReports = cards.ToDictionary(
            card => card.Id,
            card => ReadReport(card.ReportJson));
        if (cardReports.Values.Any(report => report is null))
            return CombinedTechnical("card_report_invalid");

        var cardBreakdowns = cards
            .OrderBy(card => card.CardSlot, StringComparer.Ordinal)
            .Select(card =>
            {
                var cardReport = cardReports[card.Id]!;
                return new SpeakingSimulationV11CardBreakdown(
                    card.CardSlot,
                    card.SpeakingSessionId ?? string.Empty,
                    card.Id,
                    card.EstimatedPracticeScore,
                    card.ScoreRangeLow,
                    card.ScoreRangeHigh,
                    card.ConfidenceLabel ?? "low",
                    card.ConfidenceScore,
                    cardReport.Criteria,
                    cardReport.Strengths,
                    cardReport.Weaknesses,
                    cardReport.TaskMap,
                    cardReport.Timeline,
                    cardReport.LanguageAnalysis,
                    cardReport.TimeManagement,
                    cardReport.TopFive,
                    cardReport.BetterAlternatives,
                    cardReport.Tips,
                    cardReport.PracticePlan,
                    card.SourceTranscriptId,
                    card.SourceRecordingId,
                    card.CardVersion);
            })
            .ToArray();
        var now = DateTimeOffset.UtcNow;
        var id = "spv11_combined_" + Guid.NewGuid().ToString("N");
        var criteria = new List<SpeakingSimulationV11CriterionResult>();
        foreach (var rubric in rubricCriteria)
        {
            var values = scoreRows.Where(x => x.CriterionCode == rubric.CriterionCode)
                .Select(x => x.RawScore).ToArray();
            if (values.Length != 2) return CombinedTechnical("criterion_rows_incomplete");
            var raw = Math.Round(values.Average(), 2, MidpointRounding.AwayFromZero);
            criteria.Add(new SpeakingSimulationV11CriterionResult(
                rubric.CriterionCode, rubric.Label, rubric.Weight, raw,
                Math.Round(raw * rubric.Weight / 100m, 2, MidpointRounding.AwayFromZero),
                ScoreBand(raw), "Combined two-card average of the released card scores.",
                Array.Empty<SpeakingSimulationV11EvidenceResult>()));
        }
        var overall = Math.Round(criteria.Sum(x => x.WeightedScore), 2, MidpointRounding.AwayFromZero);
        var estimated = Math.Clamp((int)Math.Round(overall * 5m, MidpointRounding.AwayFromZero), 0, 500);
        var low = Math.Max(0, (int)Math.Round(cards.Average(x => x.ScoreRangeLow ?? Math.Max(0, estimated - 25))));
        var high = Math.Min(500, (int)Math.Round(cards.Average(x => x.ScoreRangeHigh ?? Math.Min(500, estimated + 25))));
        var confidenceScore = cards.Where(x => x.ConfidenceScore.HasValue)
            .Select(x => x.ConfidenceScore!.Value).DefaultIfEmpty().Average();
        var confidence = cards.All(x => x.ConfidenceLabel == "high") ? "high"
            : cards.Any(x => x.ConfidenceLabel == "low") ? "low" : "medium";
        var report = new SpeakingSimulationV11AssessmentReport(
            id, CombinedKind, "combined", gate.SpecVersion,
            gate.RubricVersion, gate.CalibrationVersion,
            SpeakingSimulationV11Contracts.GraphDisclaimer, estimated, low, high, confidence,
            confidenceScore, "Combined practice estimate averaged across the two valid role-play cards.",
            criteria, cardBreakdowns,
            Limit(cardBreakdowns.SelectMany(x => x.Strengths), 12),
            Limit(cardBreakdowns.SelectMany(x => x.Weaknesses), 12),
            Array.Empty<SpeakingSimulationV11TaskResult>(), Array.Empty<SpeakingSimulationV11TimelineItem>(),
            new Dictionary<string, object?>(), new Dictionary<string, object?>(),
            Limit(cardBreakdowns.SelectMany(x => x.TopFive), 5),
            cardBreakdowns.SelectMany(x => x.BetterAlternatives).Take(12).ToArray(),
            cardBreakdowns.SelectMany(x => x.Tips).Distinct(StringComparer.Ordinal).Take(5).ToArray(),
            cardBreakdowns.SelectMany(x => x.PracticePlan).Take(12).ToArray(), null, null, null, now);
        var row = new SpeakingSimulationV11Assessment
        {
            Id = id, ExamSessionId = exam.Id, ProfessionId = exam.ProfessionId,
            SpecVersion = report.SpecVersion, RubricVersion = report.RubricVersion,
            CalibrationVersion = report.CalibrationVersion, AssessmentKind = CombinedKind,
            CardSlot = "combined", Status = SpeakingSimulationV11AssessmentStatus.Complete,
            AudioQualityStatus = SpeakingSimulationV11AudioQualityStatus.Passed,
            ConfidenceScore = confidenceScore, ConfidenceLabel = confidence,
            ConfidenceRange = low + "-" + high, EstimatedPracticeScore = estimated,
            ScoreRangeLow = low, ScoreRangeHigh = high, Provider = "derived",
            ModelName = "derived-card-average", PromptTemplateId = PromptTemplateId,
            GraphDisclaimer = SpeakingSimulationV11Contracts.GraphDisclaimer,
            ReportJson = JsonSerializer.Serialize(report), GeneratedAt = now,
            CreatedAt = now, UpdatedAt = now,
        };
        db.SpeakingSimulationV11Assessments.Add(row);
        foreach (var criterion in criteria)
            db.SpeakingSimulationV11CriterionScores.Add(new SpeakingSimulationV11CriterionScore
            {
                Id = "spv11_combined_score_" + Guid.NewGuid().ToString("N"),
                AssessmentId = id, CriterionCode = criterion.CriterionCode, Weight = criterion.Weight,
                RawScore = criterion.RawScore, WeightedScore = criterion.WeightedScore,
                ScoreBand = criterion.ScoreBand, Rationale = criterion.Rationale, CreatedAt = now,
            });
        await db.SaveChangesAsync(ct);
        return Project(row, report);
    }

    public async Task<SpeakingSimulationV11AssessmentResponse?> GetLatestCombinedAsync(
        string examSessionId, CancellationToken ct)
    {
        var row = await db.SpeakingSimulationV11Assessments.AsNoTracking()
            .Where(x => x.ExamSessionId == examSessionId && x.AssessmentKind == CombinedKind)
            .OrderByDescending(x => x.GeneratedAt).FirstOrDefaultAsync(ct);
        return row is null ? null : Project(row, ReadReport(row.ReportJson));
    }

    private async Task<SpeakingSimulationV11AssessmentResponse> TechnicalAsync(
        SpeakingSession session, RolePlayCard card, SpeakingSimulationV11AudioQualityStatus audioStatus,
        string code, string message, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var row = new SpeakingSimulationV11Assessment
        {
            Id = "spv11_review_" + Guid.NewGuid().ToString("N"),
            ExamSessionId = session.ExamSessionId, SpeakingSessionId = session.Id,
            RolePlayCardId = card.Id, ProfessionId = card.ProfessionId,
            SpecVersion = SpeakingSimulationV11Contracts.SpecVersion,
            RubricVersion = SpeakingSimulationV11Contracts.RubricVersion,
            CalibrationVersion = SpeakingSimulationV11Contracts.CalibrationVersion,
            AssessmentKind = CardKind,
            CardSlot = string.IsNullOrWhiteSpace(session.ExamSlot) ? "standalone" : session.ExamSlot,
            Status = SpeakingSimulationV11AssessmentStatus.TechnicalReview,
            AudioQualityStatus = audioStatus, ConfidenceLabel = "low",
            ConfidenceRange = "unavailable",
            GraphDisclaimer = SpeakingSimulationV11Contracts.GraphDisclaimer,
            TechnicalReviewCode = code, ReportJson = JsonSerializer.Serialize(new { message }),
            GeneratedAt = now, CreatedAt = now, UpdatedAt = now,
        };
        db.SpeakingSimulationV11Assessments.Add(row);
        await db.SaveChangesAsync(ct);
        return Project(row, null);
    }

    private static SpeakingSimulationV11AssessmentResponse CombinedTechnical(string code)
        => new(string.Empty, SpeakingSimulationV11AssessmentStatus.TechnicalReview.ToString(),
            CombinedKind, "combined", null, null, null,
            SpeakingSimulationV11Contracts.GraphDisclaimer, "low", null, null, code,
            DateTimeOffset.UtcNow);

    private static SpeakingSimulationV11AssessmentResponse Project(
        SpeakingSimulationV11Assessment row, SpeakingSimulationV11AssessmentReport? report)
        => new(row.Id, row.Status.ToString(), row.AssessmentKind, row.CardSlot,
            row.EstimatedPracticeScore, row.ScoreRangeLow, row.ScoreRangeHigh,
            row.GraphDisclaimer, row.ConfidenceLabel ?? "low", row.ConfidenceScore,
            report, row.TechnicalReviewCode, row.GeneratedAt);

    private static SpeakingSimulationV11AssessmentReport? ReadReport(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<SpeakingSimulationV11AssessmentReport>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException) { return null; }
    }

    private static string BuildInput(
        RolePlayCard card, IReadOnlyList<SpeakingSimulationV11TurnEvidence> turns,
        SpeakingSimulationV11CardTimingSnapshot? timing,
        IReadOnlyList<SpeakingSimulationV11RubricCriterion> rubricCriteria)
    {
        var sb = new StringBuilder();
        sb.AppendLine(AssessmentPrompt);
        sb.AppendLine("---- CANDIDATE-FACING CARD ----");
        sb.AppendLine(JsonSerializer.Serialize(new
        {
            cardId = card.Id, professionId = card.ProfessionId, scenarioTitle = card.ScenarioTitle,
            setting = card.Setting, candidateRole = card.CandidateRole,
            tasks = card.Tasks.ToArray(),
            communicationGoal = card.CommunicationGoal, clinicalTopic = card.ClinicalTopic,
        }));
        sb.AppendLine("---- RELEASED RUBRIC ----");
        sb.AppendLine(JsonSerializer.Serialize(rubricCriteria.Select(x => new
        {
            criterionCode = x.CriterionCode,
            label = x.Label,
            weight = x.Weight,
            enabledRuleIds = x.EnabledRuleIds,
        })));
        sb.AppendLine("---- AUTHORITATIVE TRANSCRIPT TURNS ----");
        sb.AppendLine(JsonSerializer.Serialize(turns.Select(x => new
        {
            turnNumber = x.TurnNumber, speaker = x.Speaker, startMs = x.StartMs, endMs = x.EndMs,
            text = x.Text, wordConfidence = JsonValue(x.WordConfidenceJson),
            interrupted = x.IsInterrupted, overlap = x.IsOverlap, monologue = x.IsMonologue,
            fillers = x.FillerCount, pauses = x.PauseCount, falseStarts = x.FalseStartCount,
            repetitions = x.RepetitionCount, jargon = x.JargonCount,
        })));
        sb.AppendLine("---- SERVER-AUTHORITATIVE TIMING ----");
        object timingPayload = timing is null
            ? new { serverAuthoritative = false, note = "timing_unavailable" }
            : new
            {
                serverAuthoritative = timing.ServerAuthoritative,
                prepSeconds = timing.PrepSeconds, rolePlaySeconds = timing.RolePlaySeconds,
                activeStartedAt = timing.ActiveStartedAt, endedAt = timing.EndedAt,
                serverElapsedSeconds = timing.ServerElapsedSeconds,
                rolePlayDeadlineAt = timing.RolePlayDeadlineAt,
            };
        sb.AppendLine(JsonSerializer.Serialize(timingPayload));
        sb.AppendLine("Use only these source turns and return the strict JSON object.");
        return sb.ToString();
    }

    internal static ParsedAssessment? ParseAssessment(
        string? completion,
        IReadOnlyList<SpeakingSimulationV11RubricCriterion>? rubricCriteria = null)
    {
        if (string.IsNullOrWhiteSpace(completion)) return null;
        try
        {
            using var document = JsonDocument.Parse(ExtractJson(completion));
            var root = document.RootElement;
            if (!root.TryGetProperty("criteria", out var array)
                || array.ValueKind != JsonValueKind.Array) return null;
            var codes = (rubricCriteria ?? SpeakingSimulationV11Contracts.RubricCriteria.Criteria)
                .Select(x => x.CriterionCode).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var parsed = new Dictionary<string, ParsedCriterion>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in array.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object
                    || !TryReadString(item, "criterionCode", out var code)
                    || !codes.Contains(code) || parsed.ContainsKey(code)) return null;
                if (!TryReadDecimal(item, "score", out var score) || score is < 0m or > 100m)
                    return null;
                var evidence = new List<ParsedEvidence>();
                if (item.TryGetProperty("evidence", out var evidenceArray)
                    && evidenceArray.ValueKind == JsonValueKind.Array)
                    foreach (var e in evidenceArray.EnumerateArray())
                        if (e.ValueKind == JsonValueKind.Object)
                            evidence.Add(new ParsedEvidence(
                                ReadString(e, "evidenceType") ?? "action",
                                ReadString(e, "quote") ?? string.Empty,
                                ReadInt(e, "turnNumber"), ReadString(e, "finding"),
                                ReadString(e, "action"), ReadString(e, "confidenceLabel"),
                                ReadDecimal(e, "confidenceScore")));
                parsed[code] = new ParsedCriterion(
                    code, score, ReadString(item, "rationale") ?? string.Empty, evidence,
                    ReadString(item, "strength"), ReadString(item, "weakness"),
                    ReadString(item, "action"), ReadString(item, "confidenceLabel"),
                    ReadDecimal(item, "confidenceScore"));
            }
            if (parsed.Count != codes.Count || codes.Any(x => !parsed.ContainsKey(x))) return null;
            var confidence = root.TryGetProperty("confidence", out var confidenceObject)
                && confidenceObject.ValueKind == JsonValueKind.Object ? confidenceObject : root;
            var low = ReadInt(confidence, "rangeLow");
            var high = ReadInt(confidence, "rangeHigh");
            if (low is < 0 or > 500 || high is < 0 or > 500) return null;
            return new ParsedAssessment(
                parsed, ReadString(root, "overallSummary"), Strings(root, "strengths"),
                Strings(root, "weaknesses"), TaskMap(root), Timeline(root),
                Dictionary(root, "languageAnalysis"), Dictionary(root, "timeManagement"),
                Strings(root, "topFive"), Alternatives(root), Strings(root, "tips"),
                PracticePlan(root), ReadString(confidence, "label") ?? "medium",
                ClampConfidence(ReadDecimal(confidence, "score")), low, high);
        }
        catch (JsonException) { return null; }
    }

    internal sealed record ParsedAssessment(
        IReadOnlyDictionary<string, ParsedCriterion> Criteria, string? Summary,
        IReadOnlyList<string> Strengths, IReadOnlyList<string> Weaknesses,
        IReadOnlyList<SpeakingSimulationV11TaskResult> TaskMap,
        IReadOnlyList<SpeakingSimulationV11TimelineItem> Timeline,
        IReadOnlyDictionary<string, object?> LanguageAnalysis,
        IReadOnlyDictionary<string, object?> TimeManagement,
        IReadOnlyList<string> TopFive, IReadOnlyList<SpeakingSimulationV11Alternative> Alternatives,
        IReadOnlyList<string> Tips, IReadOnlyList<SpeakingSimulationV11PracticePlanItem> PracticePlan,
        string ConfidenceLabel, decimal? ConfidenceScore, int? RangeLow, int? RangeHigh);

    internal sealed record ParsedCriterion(
        string Code,
        decimal Score,
        string Rationale,
        IReadOnlyList<ParsedEvidence> Evidence,
        string? Strength = null,
        string? Weakness = null,
        string? Action = null,
        string? ConfidenceLabel = null,
        decimal? ConfidenceScore = null);
    internal sealed record ParsedEvidence(
        string EvidenceType, string Quote, int? TurnNumber, string? Finding, string? Action,
        string? ConfidenceLabel, decimal? ConfidenceScore);

    private static string ExtractJson(string text)
    {
        var value = text.Trim();
        try { using var _ = JsonDocument.Parse(value); return value; }
        catch (JsonException)
        {
            var start = value.IndexOf('{'); var end = value.LastIndexOf('}');
            return start >= 0 && end > start ? value[start..(end + 1)] : value;
        }
    }

    private static SpeakingSimulationV11TurnEvidence? ResolveSource(
        IReadOnlyList<SpeakingSimulationV11TurnEvidence> candidates, ParsedEvidence evidence)
    {
        if (string.IsNullOrWhiteSpace(evidence.Quote)) return null;
        var rows = evidence.TurnNumber is { } number
            ? candidates.Where(x => x.TurnNumber == number) : candidates;
        return rows.FirstOrDefault(x => ExactQuote(x.Text, evidence.Quote));
    }

    private static IReadOnlyList<SpeakingSimulationV11Alternative> SanitizeAlternatives(
        IReadOnlyList<SpeakingSimulationV11Alternative> alternatives,
        IReadOnlyList<SpeakingSimulationV11TurnEvidence> candidates)
        => alternatives
            .Select(item =>
            {
                var source = candidates.FirstOrDefault(x => ExactQuote(x.Text, item.OriginalQuote));
                return source is null
                    ? null
                    : item with
                    {
                        OriginalQuote = source.Text,
                        TurnNumber = source.TurnNumber,
                        StartMs = ToInt(source.StartMs),
                        EndMs = ToInt(source.EndMs),
                    };
            })
            .Where(x => x is not null)
            .Cast<SpeakingSimulationV11Alternative>()
            .Take(12)
            .ToArray();

    private static IReadOnlyList<SpeakingSimulationV11TaskResult> SanitizeTaskMap(
        IReadOnlyList<SpeakingSimulationV11TaskResult> taskMap,
        IReadOnlyList<SpeakingSimulationV11TurnEvidence> candidates)
        => taskMap
            .Select(item =>
            {
                var source = string.IsNullOrWhiteSpace(item.Evidence)
                    ? null
                    : candidates.FirstOrDefault(x => ExactQuote(x.Text, item.Evidence));
                return item with { Evidence = source?.Text };
            })
            .Take(5)
            .ToArray();

    private static bool ExactQuote(string source, string quote)
    {
        if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(quote)) return false;
        if (source.Contains(quote, StringComparison.Ordinal)) return true;
        return Normalise(source).Contains(Normalise(quote), StringComparison.Ordinal);
    }

    private static string ExtractVerifiedQuote(string source, string quote)
    {
        if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(quote))
            return string.Empty;
        var start = source.IndexOf(quote, StringComparison.Ordinal);
        return start >= 0 ? source.Substring(start, quote.Length) : source;
    }

    private static string Normalise(string value)
        => string.Join(" ", value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string SourceKey(SpeakingSimulationV11TurnEvidence source)
        => string.Join("|", source.TurnNumber, source.SourceRecordingId,
            source.StartMs, source.EndMs);

    private static string EvidenceKey(
        SpeakingSimulationV11TurnEvidence source,
        ParsedEvidence evidence,
        string verifiedQuote)
        => string.Join("|", SourceKey(source),
            Normalise(evidence.EvidenceType).ToLowerInvariant(),
            Normalise(verifiedQuote).ToLowerInvariant(),
            Normalise(evidence.Finding ?? string.Empty).ToLowerInvariant());

    private static IReadOnlyList<SpeakingSimulationV11TimelineItem> BuildTimeline(
        IReadOnlyList<SpeakingSimulationV11TurnEvidence> turns)
        => turns.Take(64)
            .Select(turn => new SpeakingSimulationV11TimelineItem(
                $"{(IsCandidate(turn) ? "Candidate" : "Interlocutor")} turn {turn.TurnNumber}",
                ToInt(turn.StartMs),
                ToInt(turn.EndMs),
                Truncate(turn.Text, 240)))
            .ToArray();

    private static string Truncate(string value, int maxLength)
        => string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Length <= maxLength ? value : value[..maxLength].TrimEnd() + "…";

    private static bool IsCandidate(SpeakingSimulationV11TurnEvidence row)
        => string.Equals(row.Speaker, "candidate", StringComparison.OrdinalIgnoreCase)
            || string.Equals(row.Speaker, "learner", StringComparison.OrdinalIgnoreCase);

    private static int ToInt(long value)
        => value > int.MaxValue ? int.MaxValue : (int)Math.Max(0, value);

    private static string ConfidenceLabel(string? value)
        => value?.Trim().ToLowerInvariant() switch
        {
            "high" => "high", "low" => "low", _ => "medium",
        };

    private static decimal? ClampConfidence(decimal? value)
        => value.HasValue ? Math.Clamp(value.Value, 0m, 1m) : null;

    private static int RangeWidth(string confidence)
        => confidence == "high" ? 15 : confidence == "low" ? 40 : 25;

    private static string ScoreBand(decimal score)
        => score < 40m ? "not_met" : score < 60m ? "developing"
            : score < 80m ? "competent" : "strong";

    private static IReadOnlyList<string> Limit(IEnumerable<string> values, int count)
        => values.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim())
            .Distinct(StringComparer.Ordinal).Take(count).ToArray();

    private static bool TryReadString(JsonElement e, string name, out string value)
    {
        value = ReadString(e, name) ?? string.Empty;
        return value.Length > 0;
    }

    private static string? ReadString(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() : null;

    private static int? ReadInt(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var v)) return null;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var number)) return number;
        return v.ValueKind == JsonValueKind.String
            && int.TryParse(v.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed : null;
    }

    private static decimal? ReadDecimal(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var v)) return null;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out var number)) return number;
        return v.ValueKind == JsonValueKind.String
            && decimal.TryParse(v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed : null;
    }

    private static bool TryReadDecimal(JsonElement e, string name, out decimal value)
    {
        var parsed = ReadDecimal(e, name);
        value = parsed ?? 0m;
        return parsed.HasValue;
    }
    private static IReadOnlyList<string> Strings(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value)
            || value.ValueKind != JsonValueKind.Array) return Array.Empty<string>();
        return value.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String)
            .Select(x => x.GetString() ?? string.Empty).Where(x => x.Length > 0).ToArray();
    }

    private static IReadOnlyList<SpeakingSimulationV11TaskResult> TaskMap(JsonElement root)
    {
        if (!root.TryGetProperty("taskMap", out var value)
            || value.ValueKind != JsonValueKind.Array)
            return Array.Empty<SpeakingSimulationV11TaskResult>();
        return value.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Object)
            .Select(x => new SpeakingSimulationV11TaskResult(
                ReadInt(x, "taskNumber") ?? 0,
                ReadString(x, "status") ?? "not_met",
                ReadString(x, "evidence")))
            .Where(x => x.TaskNumber > 0).Take(5).ToArray();
    }

    private static IReadOnlyList<SpeakingSimulationV11TimelineItem> Timeline(JsonElement root)
    {
        if (!root.TryGetProperty("timeline", out var value)
            || value.ValueKind != JsonValueKind.Array)
            return Array.Empty<SpeakingSimulationV11TimelineItem>();
        return value.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Object)
            .Select(x => new SpeakingSimulationV11TimelineItem(
                ReadString(x, "label") ?? "event", ReadInt(x, "startMs"),
                ReadInt(x, "endMs"), ReadString(x, "note")))
            .Take(32).ToArray();
    }

    private static IReadOnlyDictionary<string, object?> Dictionary(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value)
            || value.ValueKind != JsonValueKind.Object)
            return new Dictionary<string, object?>();
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
            result[property.Name] = JsonSerializer.Deserialize<object>(property.Value.GetRawText());
        return result;
    }

    private static IReadOnlyList<SpeakingSimulationV11Alternative> Alternatives(JsonElement root)
    {
        if (!root.TryGetProperty("betterAlternatives", out var value)
            || value.ValueKind != JsonValueKind.Array)
            return Array.Empty<SpeakingSimulationV11Alternative>();
        return value.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Object)
            .Select(x => new SpeakingSimulationV11Alternative(
                ReadString(x, "originalQuote") ?? string.Empty,
                ReadString(x, "betterAlternative") ?? string.Empty,
                ReadInt(x, "turnNumber"), ReadInt(x, "startMs"), ReadInt(x, "endMs")))
            .Where(x => x.OriginalQuote.Length > 0 && x.BetterAlternative.Length > 0)
            .Take(12).ToArray();
    }

    private static IReadOnlyList<SpeakingSimulationV11PracticePlanItem> PracticePlan(JsonElement root)
    {
        if (!root.TryGetProperty("practicePlan", out var value)
            || value.ValueKind != JsonValueKind.Array)
            return Array.Empty<SpeakingSimulationV11PracticePlanItem>();
        return value.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Object)
            .Select(x => new SpeakingSimulationV11PracticePlanItem(
                ReadString(x, "focus") ?? string.Empty, ReadString(x, "action") ?? string.Empty,
                ReadString(x, "frequency") ?? string.Empty,
                ReadString(x, "successMeasure") ?? string.Empty))
            .Where(x => x.Focus.Length > 0 && x.Action.Length > 0).Take(12).ToArray();
    }

    private static JsonElement JsonValue(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return JsonDocument.Parse("null").RootElement.Clone();
        try { return JsonDocument.Parse(json).RootElement.Clone(); }
        catch (JsonException) { return JsonDocument.Parse("null").RootElement.Clone(); }
    }

}
